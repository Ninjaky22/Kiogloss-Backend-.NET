#!/usr/bin/env python3
"""
Parity harness: runs the same scripted scenario against the Spring backend (A) and the .NET
backend (B), each one pointing to its own copy of the SAME database snapshot, and compares
status codes and JSON bodies.

    python3 tests/parity/parity.py --a http://localhost:8100/API/V1.0 --b http://localhost:8001/API/V1.0

Only things that legitimately differ between two runs are normalized: JWT values, timestamps
(compared by shape), host:port inside upload URLs and the identity hash Spring prints inside
validation messages. Everything else — including JSON key order — is compared.

Endpoints that would hit real external services (Cloudinary uploads, Gmail) are only exercised
through their error paths.
"""
import argparse
import http.client
import json
import re
import socket
import sys
import time
import urllib.parse
import uuid

JWT_RE = re.compile(r"^eyJ[\w-]+\.[\w-]+\.[\w-]+$")
DATETIME_RE = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?$")
DATETIME_KEYS = {"createdAt", "published", "timestamp"}


class Num(str):
    """A JSON number with a fraction, compared by its exact text."""


class Client:
    def __init__(self, base):
        u = urllib.parse.urlparse(base)
        self.host, self.port, self.prefix = u.hostname, u.port or 80, u.path.rstrip("/")

    def request(self, method, path, body=None, token=None, headers=None, raw=None, content_type="application/json"):
        h = dict(headers or {})
        data = None
        if token:
            h["Authorization"] = "Bearer " + token
        if raw is not None:
            data = raw
            if content_type:
                h["Content-Type"] = content_type
        elif body is not None:
            data = json.dumps(body).encode()
            h["Content-Type"] = content_type
        conn = http.client.HTTPConnection(self.host, self.port, timeout=30)
        conn.request(method, self.prefix + path, body=data, headers=h)
        r = conn.getresponse()
        payload = r.read()
        conn.close()
        ctype = (r.getheader("Content-Type") or "").split(";")[0]
        try:
            # numbers with a fraction/exponent are kept as text: "1700000.00" != "1700000.0000", "90000.0" != "90000"
            parsed = json.loads(payload, parse_float=lambda s: Num(s)) if payload else None
        except ValueError:
            parsed = payload.decode(errors="replace")
        return r.status, ctype, parsed

    def sse(self, path, token, accept="text/event-stream", trigger=None, seconds=3.0):
        """Opens an SSE stream, optionally runs trigger() after the first frame, returns status + raw text."""
        s = socket.create_connection((self.host, self.port), timeout=seconds + 2)
        q = urllib.parse.quote(token)
        hdrs = f"GET {self.prefix}{path}?token={q} HTTP/1.1\r\nHost: {self.host}:{self.port}\r\n"
        if accept:
            hdrs += f"Accept: {accept}\r\n"
        s.sendall((hdrs + "Connection: close\r\n\r\n").encode())
        buf = b""
        deadline = time.time() + seconds
        triggered = False
        s.settimeout(0.3)
        while time.time() < deadline:
            try:
                chunk = s.recv(65536)
                if not chunk:
                    break
                buf += chunk
            except socket.timeout:
                pass
            if trigger and not triggered and b"connected" in buf and b"ok" in buf.split(b"connected", 1)[1]:
                triggered = True
                trigger()
        s.close()
        head, _, rest = buf.partition(b"\r\n\r\n")
        status = int(head.split(b" ")[1]) if head else 0
        if b"chunked" in head.lower():
            body, pos = b"", 0
            while True:
                end = rest.find(b"\r\n", pos)
                if end < 0:
                    break
                try:
                    size = int(rest[pos:end].split(b";")[0], 16)
                except ValueError:
                    break
                if size == 0:
                    break
                body += rest[end + 2:end + 2 + size]
                pos = end + 2 + size + 2
            rest = body
        text = rest.decode(errors="replace")
        frames = [line for line in text.split("\n") if line and not line.startswith(":heartbeat")]
        return status, frames


# ------------------------------------------------------------------ normalization / diff

def shape(dt):
    return re.sub(r"\d", "d", re.sub(r"\.\d+$", ".f", dt))


UNORDERED_LISTS = {"options", "variants", "items"}
# Values Java picks from a HashSet of entities with identity hash codes (random per JVM run).
IGNORED = {
    "fav-list": {"$.account.favorite[0].defaultVariantId"},  # findFirst() over Product#getVariants of a multi-variant product
}
UNORDERED_LABELS = {"variantDetails", "size"}


def normalize(value, key=None):
    if isinstance(value, dict):
        return {k: normalize(v, k) for k, v in value.items()}
    if isinstance(value, list):
        items = [normalize(v) for v in value]
        if key in UNORDERED_LISTS and all(isinstance(i, dict) and "id" in i for i in items):
            items.sort(key=lambda i: (i["id"] is None, i["id"] or 0))
        if key == "recentOrders":
            # Java sorts by date DESC over a HashSet: ties keep the (random) identity-hash order
            items.sort(key=lambda i: i["id"])
            items.sort(key=lambda i: i["date"], reverse=True)
        return items
    if isinstance(value, str) and key in UNORDERED_LABELS:
        return ", ".join(sorted(value.split(", ")))
    if isinstance(value, str):
        if JWT_RE.match(value):
            return "<jwt>"
        if key in DATETIME_KEYS and DATETIME_RE.match(value):
            return "<datetime " + shape(value) + ">"
        if key == "url" and "/uploads/" in value:
            return "<host>/uploads/<file>" + re.sub(r"^.*?(\.[^.]*)?$", r"\1", value)
        v = re.sub(r"@[0-9a-f]{6,8}\b", "@<hash>", value)
        if "MethodArgumentNotValidException" in v:
            head, sep, errors = v.partition(": [")
            parts = sorted(re.findall(r"\[Field error.*?\]\] ", "[" + errors))
            v = head + sep + "".join(parts)
        if "HttpMessageNotReadableException: JSON parse error:" in v:
            v = v[:v.index("JSON parse error:") + len("JSON parse error:")] + " <parser message>"
        if "ExpiredJwtException" in v:
            v = re.sub(r"\d+ milliseconds ago at .*?Z\. Current time: .*?Z", "<n> ms ago", v)
        return v
    return value


def diff(a, b, path="$"):
    out = []
    if isinstance(a, dict) and isinstance(b, dict):
        if list(a.keys()) != list(b.keys()) and set(a.keys()) == set(b.keys()):
            out.append(("order", path, list(a.keys()), list(b.keys())))
        for k in a.keys() | b.keys():
            if k not in a:
                out.append(("extra-in-B", f"{path}.{k}", None, b[k]))
            elif k not in b:
                out.append(("missing-in-B", f"{path}.{k}", a[k], None))
            else:
                out += diff(a[k], b[k], f"{path}.{k}")
    elif isinstance(a, list) and isinstance(b, list):
        if len(a) != len(b):
            out.append(("length", path, len(a), len(b)))
        for i, (x, y) in enumerate(zip(a, b)):
            out += diff(x, y, f"{path}[{i}]")
    elif isinstance(a, bool) or isinstance(b, bool):
        if a is not b:
            out.append(("value", path, a, b))
    elif isinstance(a, (int, float)) and isinstance(b, (int, float)):
        if float(a) != float(b):
            out.append(("value", path, a, b))
    elif a != b:
        out.append(("value", path, a, b))
    return out


# ------------------------------------------------------------------ scenario

class Run:
    """Executes the scenario against one backend, recording every response by step name."""

    def __init__(self, client, tag):
        self.c, self.tag, self.results, self.ctx = client, tag, {}, {}

    def step(self, name, method, path, **kw):
        status, ctype, body = self.c.request(method, path, **kw)
        self.results[name] = {"status": status, "contentType": ctype, "body": body}
        return status, body

    def scenario(self, uniq):
        r = self.step
        email_user, email_admin = f"parity.user.{uniq}@test.com", f"parity.admin.{uniq}@test.com"
        phone_user, phone_admin = "3" + uniq[:9], "3" + uniq[-9:]
        # ---------- public catalog
        r("products", "GET", "/products")
        r("products-price-low", "GET", "/products?sort=price-low")
        r("products-price-high", "GET", "/products?sort=price-high&page=0&page_size=1")
        r("products-search", "GET", "/products?search=gamer%20xx")
        r("products-search-short", "GET", "/products?search=ab")
        r("products-tags", "GET", "/products?tags=" + urllib.parse.quote("Tecnología,Maquillaje"))
        r("products-bad-page", "GET", "/products?page=-1")
        r("products-bad-size", "GET", "/products?page_size=0")
        r("products-page-abc", "GET", "/products?page=abc")
        r("products-trailing-slash", "GET", "/products/")
        r("article", "GET", "/article/tarjeta-grafica-gamer")
        r("article-missing", "GET", "/article/no-existe")
        r("tags", "GET", "/tags")
        r("videos", "GET", "/videos")
        r("users-public-list", "GET", "/user/")
        r("users-no-slash", "GET", "/user")
        r("unknown-anon", "GET", "/nonexistent")
        r("login-get", "GET", "/login")
        # ---------- registration
        _, body = r("register-user", "POST", "/user/", body={
            "email": email_user, "name": "Parity User", "password": "Secret123!", "phoneNumber": phone_user,
            "address": {"street": "Calle 9", "streetNumber": "1-2", "distric": "Sur", "city": "Neiva"},
            "account": {"pointsPerPurchase": 0, "isActive": True}})
        r("register-admin", "POST", "/user/", body={
            "email": email_admin, "name": "Parity Admin", "password": "Secret123!", "phoneNumber": phone_admin,
            "isSuperuser": True})
        r("register-duplicate-email", "POST", "/user/", body={"email": email_user, "name": "x", "password": "x"})
        r("register-duplicate-phone", "POST", "/user/", body={"email": "dup." + email_user, "name": "x", "password": "x", "phoneNumber": phone_user})
        r("register-bad-phone", "POST", "/user/", body={"email": "bad." + email_user, "name": "x", "password": "x", "phoneNumber": "12ab"})
        r("register-bad-email", "POST", "/user/", body={"email": "notanemail", "name": "x", "password": "x"})
        r("register-empty-body", "POST", "/user/", raw=b"")
        r("register-malformed", "POST", "/user/", raw=b"{nojson")
        r("register-text-plain", "POST", "/user/", raw=b"hola", content_type="text/plain")
        # ---------- auth
        _, login = r("login", "POST", "/login", body={"email": email_user, "password": "Secret123!"})
        _, alogin = r("login-admin", "POST", "/login", body={"email": email_admin, "password": "Secret123!"})
        r("login-bad-password", "POST", "/login", body={"email": email_user, "password": "nope"})
        r("login-unknown", "POST", "/login", body={"email": "nobody@test.com", "password": "nope"})
        r("login-not-email", "POST", "/login", body={"email": "nobody", "password": ""})
        token, admin = login["access"], alogin["access"]
        self.ctx.update(token=token, admin=admin, refresh=login["refresh"])
        r("refresh", "POST", "/refresh", body={"refresh": login["refresh"]})
        r("refresh-garbage", "POST", "/refresh", body={"refresh": "abc"})
        r("refresh-null", "POST", "/refresh", body={})
        r("forgot-unknown-email", "POST", "/auth/forgot-password", body={"email": "nobody@test.com"})
        r("forgot-invalid", "POST", "/auth/forgot-password", body={"email": "x"})
        r("reset-bad-token", "POST", "/auth/reset-password", body={"token": "nope", "newPassword": "12345678"})
        r("reset-short", "POST", "/auth/reset-password", body={"token": "nope", "newPassword": "123"})
        # ---------- user
        _, me = r("register-user-detail-lookup", "GET", "/user/?search=" + urllib.parse.quote(email_user), token=token)
        uid = next(u["id"] for u in me["content"] if u["email"] == email_user) if isinstance(me, dict) else 0
        r("user-detail", "GET", f"/user/{uid}", token=token)
        r("user-detail-other", "GET", f"/user/{uid + 1}", token=token)
        r("user-detail-abc", "GET", "/user/abc", token=token)
        r("user-detail-anon", "GET", f"/user/{uid}")
        r("user-update", "PATCH", f"/user/update/{uid}", token=token, body={"name": "Parity User 2"})
        r("user-update-bad-phone", "PATCH", f"/user/update/{uid}", token=token, body={"phoneNumber": "12ab"})
        r("user-update-address", "PATCH", f"/user/update/{uid}", token=token,
          body={"address": {"street": "Cra 1", "streetNumber": "2", "distric": "Centro", "city": None}})
        _, detail = r("user-detail-after", "GET", f"/user/{uid}", token=token)
        acc = detail["account"]["id"]
        # ---------- admin catalog setup
        r("admin-anon", "GET", "/admin/products")
        r("admin-as-user", "GET", "/admin/products", token=token)
        _, tag = r("tag-create", "POST", "/admin/tags", token=admin, body={"name": "Parity " + uniq, "imageURL": None})
        r("tag-create-invalid", "POST", "/admin/tags", token=admin, body={"name": " "})
        r("tag-update", "PUT", f"/admin/tags/{tag['id']}", token=admin, body={"imageURL": "https://img/x.png"})
        r("tags-admin", "GET", "/admin/tags", token=admin)
        _, vt = r("vt-create", "POST", "/admin/variants/types", token=admin, body={
            "name": "Parity VT " + uniq, "type": "color", "options": [{"value": "Rojo", "metaValue": "#ff0000"}]})
        rojo = vt["options"][0]["id"]
        _, vt = r("vt-add-option", "PUT", f"/admin/variants/types/{vt['id']}", token=admin, body={
            "name": "Parity VT " + uniq, "type": "color",
            "options": [{"id": rojo, "value": "Rojo", "metaValue": "#ff0000"}, {"value": "Azul", "metaValue": "#0000ff"}]})
        azul = next(o["id"] for o in vt["options"] if o["value"] == "Azul")
        opt = [rojo, azul]
        r("vt-dup", "POST", "/admin/variants/types", token=admin, body={"name": "Parity VT " + uniq, "type": "x", "options": []})
        _, prod = r("product-create", "POST", "/admin/products", token=admin, body={
            "title": "Parity Prod " + uniq, "price": 12345, "description": "<p>d</p>", "slug": "parity-" + uniq,
            "status": "published", "tagIds": [tag["id"]], "attributes": {"Marca": "Kio", "Peso": "5g", "Color": "Rojo"},
            "variants": [{"optionIds": [opt[0]], "stock": 4, "sku": "P-R-" + uniq}]})
        pid = prod["id"]
        by_sku = lambda resp, sku: next(v["id"] for v in resp["variants"] if v["sku"] == sku)
        _, prod = r("product-add-variant", "PUT", f"/admin/products/{pid}", token=admin, body={
            "variants": [{"id": by_sku(prod, "P-R-" + uniq), "optionIds": [opt[0]], "stock": 4, "sku": "P-R-" + uniq},
                         {"optionIds": [opt[1]], "stock": 9, "sku": "P-A-" + uniq}]})
        _, simple = r("product-create-simple", "POST", "/admin/products", token=admin, body={
            "title": "Parity Simple " + uniq, "price": "999.50", "description": "s", "slug": "parity-simple-" + uniq, "stock": 6,
            "status": "published"})
        r("product-create-dup-slug", "POST", "/admin/products", token=admin, body={
            "title": "x", "price": 1, "description": "d", "slug": "parity-" + uniq})
        r("product-create-invalid", "POST", "/admin/products", token=admin, body={"title": "", "price": None})
        _, many = r("product-create-many-attrs", "POST", "/admin/products", token=admin, body={
            "title": "Parity Attrs " + uniq, "price": 10, "description": "a", "slug": "parity-attrs-" + uniq, "status": "published",
            "tagIds": [1, tag["id"]],
            "attributes": {"Marca": "Kio", "Peso": "5g", "Color": "Rojo", "Origen": "CO", "Material": "Seda", "Garantia": "1 año", "Talla": "M"}})
        r("product-get-many-attrs", "GET", f"/admin/products/{many['id']}", token=admin)
        r("article-many-attrs", "GET", "/article/parity-attrs-" + uniq)
        r("product-update-many-attrs", "PUT", f"/admin/products/{many['id']}", token=admin, body={
            "attributes": {"Zeta": "1", "Alfa": "2", "Peso": "6g"}, "tagIds": [tag["id"], 1]})
        r("article-many-attrs-after", "GET", "/article/parity-attrs-" + uniq)
        r("product-get", "GET", f"/admin/products/{pid}", token=admin)
        r("product-get-missing", "GET", "/admin/products/999999", token=admin)
        r("products-admin", "GET", "/admin/products?page=0&page_size=100", token=admin)
        r("products-admin-search", "GET", "/admin/products?search=parity", token=admin)
        _, upd = r("product-update", "PUT", f"/admin/products/{pid}", token=admin, body={
            "title": "Parity Prod Upd " + uniq, "price": 20000.5, "attributes": {"Peso": "6g", "Origen": "CO"},
            "variants": [{"id": by_sku(prod, "P-R-" + uniq), "optionIds": [opt[0], opt[1]], "stock": 3, "sku": "P-R-" + uniq},
                         {"id": by_sku(prod, "P-A-" + uniq), "optionIds": [opt[1]], "stock": 9, "sku": "P-A-" + uniq}]})
        r("product-update-tags", "PUT", f"/admin/products/{simple['id']}", token=admin, body={"tagIds": [], "variants": [], "stock": 11})
        r("product-detail-public", "GET", "/article/parity-" + uniq)
        r("products-public-after", "GET", "/products?page_size=50")
        r("products-search-after", "GET", "/products?search=parity%20rojo")
        r("vt-update", "PUT", f"/admin/variants/types/{vt['id']}", token=admin, body={
            "name": "Parity VT " + uniq, "type": "color",
            "options": [{"id": opt[0], "value": "Rojo2", "metaValue": "#f00"}, {"value": "Verde", "metaValue": "#0f0"}]})
        _, vt3 = r("vt-create-free", "POST", "/admin/variants/types", token=admin, body={"name": "Free " + uniq, "type": "button", "options": [{"value": "S"}]})
        r("vt-remove-unused-option", "PUT", f"/admin/variants/types/{vt3['id']}", token=admin, body={"name": "Free " + uniq, "type": "select", "options": []})
        r("vt-delete-in-use", "DELETE", f"/admin/variants/types/{vt['id']}", token=admin)
        _, vt2 = r("vt-create-2", "POST", "/admin/variants/types", token=admin, body={"name": "Tmp " + uniq, "type": None, "options": [{"value": "t"}]})
        r("vt-delete-free", "DELETE", f"/admin/variants/types/{vt2['id']}", token=admin)
        r("vts", "GET", "/admin/variants/types", token=admin)
        r("tag-delete-in-use", "DELETE", f"/admin/tags/{tag['id']}", token=admin)
        _, tag2 = r("tag-create-2", "POST", "/admin/tags", token=admin, body={"name": "Tmp " + uniq})
        r("tag-delete-free", "DELETE", f"/admin/tags/{tag2['id']}", token=admin)
        r("image-upload-missing", "POST", "/admin/products/images", token=admin, body={"imageBase64": ""})
        r("image-delete-missing", "DELETE", "/admin/products/images/999999", token=admin)
        # ---------- cart
        var_a = by_sku(upd, "P-R-" + uniq)
        _, plist = r("products-for-cart", "GET", "/products?page_size=50")
        simple_variant = next((p["defaultVariantId"] for p in plist["content"] if p["slug"] == "parity-simple-" + uniq), None)
        r("cart-add", "POST", "/user/cart", token=token, body={"variantId": var_a, "quantity": 1})
        r("cart-add-again", "POST", "/user/cart", token=token, body={"variantId": str(var_a), "quantity": "2"})
        r("cart-add-simple", "POST", "/user/cart", token=token, body={"variantId": simple_variant, "quantity": 1})
        r("cart-add-bad", "POST", "/user/cart", token=token, body={"variantId": "abc", "quantity": 1})
        r("cart-add-float", "POST", "/user/cart", token=token, body={"variantId": 1.0, "quantity": 1})
        r("cart-add-missing", "POST", "/user/cart", token=token, body={"quantity": 1})
        r("cart-add-unknown-variant", "POST", "/user/cart", token=token, body={"variantId": 999999, "quantity": 1})
        _, cart = r("cart", "GET", "/user/cart?page=0&page_size=200", token=token)
        first_item = cart["content"][0]["id"] if isinstance(cart, dict) and cart.get("content") else 0
        r("cart-put", "PUT", f"/user/cart/{first_item}?quantity=2", token=token)
        r("cart-put-no-qty", "PUT", f"/user/cart/{first_item}", token=token)
        r("cart-put-missing", "PUT", "/user/cart/999999?quantity=1", token=token)
        r("cart-delete-missing", "DELETE", "/user/cart/999999", token=token)
        r("cart-after", "GET", "/user/cart", token=token)
        r("cart-anon", "GET", "/user/cart")
        # ---------- favorites
        _, fav = r("fav-add", "POST", "/user/favorite", token=token, body={"account": acc, "product": pid})
        r("fav-add-invalid", "POST", "/user/favorite", token=token, body={"account": "", "product": pid})
        r("fav-add-missing-product", "POST", "/user/favorite", token=token, body={"account": acc, "product": 999999})
        r("fav-list", "GET", f"/user/account/account_favorites/{acc}", token=token)
        r("user-detail-favs", "GET", f"/user/{uid}", token=token)
        # ---------- orders (+ notifications)
        r("order-create", "POST", "/user/order", token=token, body={
            "account": acc, "shopping": [{"product": var_a, "quantity": 2, "price": 40001}], "amount": 40001, "status": "PENDING"})
        r("order-create-processing", "POST", "/user/order", token=token, body={
            "account": acc, "shopping": [{"product": simple_variant, "quantity": 1, "price": 999.5}], "amount": 999.5, "status": "processing"})
        r("order-no-stock", "POST", "/user/order", token=token, body={
            "account": acc, "shopping": [{"product": var_a, "quantity": 500, "price": 1}], "amount": 1, "status": "PENDING"})
        r("order-bad-status", "POST", "/user/order", token=token, body={"account": acc, "shopping": [], "amount": 1, "status": "Pendiente"})
        r("order-invalid", "POST", "/user/order", token=token, body={"account": acc})
        r("order-empty", "POST", "/user/order", token=token, raw=b"")
        r("orders", "GET", "/user/orders", token=token)
        r("orders-filter", "GET", "/user/orders?statusOrder=Pendiente&rangeDate_after=2000-01-01&rangeDate_before=2999-12-31&page=0&page_size=10", token=token)
        r("orders-filter-all", "GET", "/user/orders?statusOrder=ALL", token=token)
        r("orders-bad-date", "GET", "/user/orders?rangeDate_after=2026-13-01", token=token)
        r("orders-bad-status", "GET", "/user/orders?statusOrder=xyz", token=token)
        r("orders-bad-page", "GET", "/user/orders?page=abc", token=token)
        # ---------- admin users / orders / stats / reports
        r("admin-users", "GET", "/admin/users", token=admin)
        r("admin-users-search", "GET", "/admin/users?search=PARITY&sortBy=email&sortDir=DESC&page=0&page_size=5", token=admin)
        r("admin-users-bad-sort", "GET", "/admin/users?sortBy=xyz", token=admin)
        r("admin-user-detail", "GET", f"/admin/users/{uid}", token=admin)
        r("admin-user-missing", "GET", "/admin/users/999999", token=admin)
        r("admin-user-roles", "PUT", f"/admin/users/{uid}/roles", token=admin, body={"isStaff": True, "isSuperuser": False})
        r("admin-user-roles-patch", "PATCH", f"/admin/users/{uid}/roles", token=admin, body={"isStaff": True})
        r("admin-user-toggle", "PATCH", f"/admin/users/{uid}/toggle-status", token=admin)
        r("admin-user-toggle-back", "PATCH", f"/admin/users/{uid}/toggle-status", token=admin)
        _, orders = r("admin-orders", "GET", "/admin/orders?page=0&page_size=50", token=admin)
        r("admin-orders-pending", "GET", "/admin/orders?status=PENDING", token=admin)
        r("admin-orders-spanish", "GET", "/admin/orders?status=Pendiente", token=admin)
        r("admin-orders-user", "GET", f"/admin/orders?userId={uid}", token=admin)
        oid = next((o["id"] for o in orders["data"]["content"] if o["customer"]["email"] == email_user), 0) \
            if isinstance(orders, dict) else 0
        r("admin-order-detail", "GET", f"/admin/orders/{oid}", token=admin)
        r("admin-order-missing", "GET", "/admin/orders/999999", token=admin)
        r("admin-user-orders", "GET", f"/admin/users/{uid}/orders", token=admin)
        r("admin-user-orders-missing", "GET", "/admin/users/999999/orders", token=admin)
        r("admin-order-status", "PATCH", f"/admin/orders/{oid}/status?status=SHIPPED", token=admin)
        r("admin-order-status-bad", "PATCH", f"/admin/orders/{oid}/status?status=foo", token=admin)
        r("admin-order-status-missing", "PATCH", f"/admin/orders/{oid}/status", token=admin)
        r("admin-stats", "GET", "/admin/stats", token=admin)
        r("report-summary", "GET", "/admin/reports/summary", token=admin)
        r("report-sales", "GET", "/admin/reports/sales-by-day?days=7", token=admin)
        r("report-top", "GET", "/admin/reports/top-products?limit=10", token=admin)
        r("report-top-zero", "GET", "/admin/reports/top-products?limit=0", token=admin)
        r("report-status", "GET", "/admin/reports/orders-by-status", token=admin)
        r("report-as-user", "GET", "/admin/reports/summary", token=token)
        r("admin-unknown", "GET", "/admin/sizes", token=admin)
        # ---------- notifications
        _, notes = r("notif-user", "GET", "/user/notifications", token=token)
        r("notif-user-count", "GET", "/user/notifications/unread-count", token=token)
        _, anotes = r("notif-admin", "GET", "/admin/notifications?page=0&page_size=5", token=admin)
        r("notif-admin-count", "GET", "/admin/notifications/unread-count", token=admin)
        if isinstance(anotes, dict) and anotes.get("content"):
            nid = anotes["content"][0]["id"]
            r("notif-admin-read", "PATCH", f"/admin/notifications/{nid}/read", token=admin)
            r("notif-admin-delete", "DELETE", f"/admin/notifications/{nid}", token=admin)
        r("notif-admin-read-all", "PATCH", "/admin/notifications/read-all", token=admin)
        r("notif-admin-after", "GET", "/admin/notifications/unread-count", token=admin)
        r("notif-user-read-all", "PATCH", "/user/notifications/read-all", token=token)
        r("notif-admin-as-user", "GET", "/admin/notifications", token=token)
        # ---------- videos
        _, video = r("video-create", "POST", "/admin/videos", token=admin, body={
            "videoUrl": "https://cdn/v.mp4", "thumbnailUrl": "https://cdn/t.jpg", "username": "@kio", "productId": pid})
        r("video-create-invalid", "POST", "/admin/videos", token=admin, body={"username": "", "productId": None})
        r("video-create-missing-product", "POST", "/admin/videos", token=admin, body={
            "thumbnailUrl": "t", "username": "u", "productId": 999999})
        r("video-update", "PUT", f"/admin/videos/{video['id']}", token=admin, body={"username": "@kio2"})
        r("videos-admin", "GET", "/admin/videos", token=admin)
        r("videos-public", "GET", "/videos")
        r("video-delete", "DELETE", f"/admin/videos/{video['id']}", token=admin)
        r("video-delete-missing", "DELETE", "/admin/videos/999999", token=admin)
        # ---------- uploads (local disk only)
        boundary = "----parity" + uniq
        multipart = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"pic.png\"\r\n"
                     f"Content-Type: image/png\r\n\r\nPNGDATA\r\n--{boundary}--\r\n").encode()
        r("upload", "POST", "/admin/upload", token=admin, raw=multipart, content_type=f"multipart/form-data; boundary={boundary}")
        r("upload-not-multipart", "POST", "/admin/upload", token=admin, body={})
        # ---------- deletes with integrity errors
        r("product-delete-in-use", "DELETE", f"/admin/products/{pid}", token=admin)
        _, tmp = r("product-create-tmp", "POST", "/admin/products", token=admin, body={
            "title": "Tmp", "price": 1, "description": "d", "slug": "tmp-" + uniq, "tagIds": [tag["id"]], "attributes": {"a": "b"}})
        r("product-delete-free", "DELETE", f"/admin/products/{tmp['id']}", token=admin)
        r("product-delete-missing", "DELETE", "/admin/products/999999", token=admin)
        r("fav-remove", "DELETE", f"/user/delete/{fav['idFa']}", token=token)
        r("fav-remove-missing", "DELETE", "/user/delete/999999", token=token)
        r("cart-clear", "DELETE", "/user/cart", token=token)
        r("cart-final", "GET", "/user/cart", token=token)
        # ---------- SSE
        _, _, mine = self.c.request("GET", "/user/orders", token=token)
        my_order = mine["content"][0]["id"] if isinstance(mine, dict) and mine.get("content") else oid
        status, frames = self.c.sse("/user/notifications/stream", token, trigger=lambda: self.c.request(
            "PATCH", f"/admin/orders/{my_order}/status?status=DELIVERED", token=admin))
        self.results["sse-user"] = {"status": status, "contentType": "text/event-stream",
                                    "body": [re.sub(r'"createdAt":"[^"]*"', '"createdAt":"<datetime>"', f) for f in frames]}
        status, frames = self.c.sse("/user/notifications/stream", token, accept=None, seconds=1.0)
        self.results["sse-no-accept"] = {"status": status, "contentType": "", "body": frames}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--a", required=True, help="Spring base URL, e.g. http://localhost:8100/API/V1.0")
    ap.add_argument("--b", required=True, help=".NET base URL, e.g. http://localhost:8001/API/V1.0")
    ap.add_argument("--report", default="parity-report.json")
    ap.add_argument("--show-order", action="store_true", help="also print JSON key-order differences")
    args = ap.parse_args()

    uniq = uuid.uuid4().hex[:12]
    uniq = "".join(c for c in uniq if c.isdigit()).ljust(12, "7")[:12]
    a, b = Run(Client(args.a), "A"), Run(Client(args.b), "B")
    for run in (a, b):
        try:
            run.scenario(uniq)
        except Exception as e:  # keep what ran so far
            print(f"[{run.tag}] scenario aborted: {e!r}")

    report, failures = [], 0
    for name in a.results.keys() | b.results.keys():
        ra, rb = a.results.get(name), b.results.get(name)
        if ra is None or rb is None:
            report.append({"step": name, "result": "MISSING", "A": ra, "B": rb})
            failures += 1
            continue
        problems = []
        if ra["status"] != rb["status"]:
            problems.append(("status", "$", ra["status"], rb["status"]))
        if (ra["contentType"] or "") != (rb["contentType"] or ""):
            problems.append(("contentType", "$", ra["contentType"], rb["contentType"]))
        problems += [p for p in diff(normalize(ra["body"]), normalize(rb["body"])) if p[1] not in IGNORED.get(name, ())]
        # Jackson orders PageImpl properties by reflection order, which changes between JVM runs
        page_order = lambda p: p[0] == "order" and set(p[2]) >= {"pageable", "numberOfElements", "first"}
        problems = [p for p in problems if not page_order(p)]
        hard = [p for p in problems if p[0] != "order"]
        result = "DIFF" if hard else ("ORDER" if problems else "OK")
        failures += bool(hard)
        report.append({"step": name, "result": result, "status": [ra["status"], rb["status"]],
                       "problems": [list(map(str, p)) for p in problems], "A": ra["body"], "B": rb["body"]})

    order = {n: i for i, n in enumerate(a.results)}
    report.sort(key=lambda r: order.get(r["step"], 10 ** 6))
    for r in report:
        if r["result"] == "OK" or (r["result"] == "ORDER" and not args.show_order):
            continue
        print(f"{r['result']:6} {r['step']}  status={r.get('status')}")
        for p in r.get("problems", [])[:8]:
            if p[0] == "order" and not args.show_order:
                continue
            print("        ", " | ".join(x[:300] for x in p))
    ok = sum(r["result"] == "OK" for r in report)
    ordered = sum(r["result"] == "ORDER" for r in report)
    print(f"\n{len(report)} steps: {ok} identical, {ordered} only key order, {failures} different")
    with open(args.report, "w") as f:
        json.dump(report, f, indent=2, ensure_ascii=False, default=str)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
