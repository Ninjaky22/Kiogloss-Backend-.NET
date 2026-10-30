# ---- build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Kiogloss.sln ./
COPY src/Kiogloss.Api/Kiogloss.Api.csproj src/Kiogloss.Api/
RUN dotnet restore src/Kiogloss.Api/Kiogloss.Api.csproj
COPY src/ src/
RUN dotnet publish src/Kiogloss.Api/Kiogloss.Api.csproj -c Release -o /app --no-restore

# ---- runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
# The Spring backend ran with the JVM default time zone: LocalDate/LocalDateTime values (order dates,
# notifications, "today" in reports) are server-local. Keep the same zone here.
ENV TZ=America/Bogota \
    Urls=http://0.0.0.0:8000 \
    App__UploadDir=/app/uploads
COPY --from=build /app ./
RUN mkdir -p /app/uploads
VOLUME ["/app/uploads"]
EXPOSE 8000
ENTRYPOINT ["dotnet", "Kiogloss.Api.dll"]
