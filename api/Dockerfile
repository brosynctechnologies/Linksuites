# Build: docker build -t saas-api ./api
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Saas.slnx ./
COPY src/Saas.Domain/Saas.Domain.csproj src/Saas.Domain/
COPY src/Saas.Application/Saas.Application.csproj src/Saas.Application/
COPY src/Saas.Infrastructure/Saas.Infrastructure.csproj src/Saas.Infrastructure/
COPY src/Saas.Api/Saas.Api.csproj src/Saas.Api/
RUN dotnet restore src/Saas.Api/Saas.Api.csproj
COPY src/ src/
RUN dotnet publish src/Saas.Api/Saas.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
ENV Video__FfmpegPath=ffmpeg
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Saas.Api.dll"]
