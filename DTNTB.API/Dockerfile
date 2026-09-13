FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["DTNTB.API/DTNTB.API.csproj", "DTNTB.API/"]
COPY ["DTNTB.Core/DTNTB.Core.csproj", "DTNTB.Core/"]
COPY ["DTNTB.Infrastructure/DTNTB.Infrastructure.csproj", "DTNTB.Infrastructure/"]
RUN dotnet restore "DTNTB.API/DTNTB.API.csproj"

COPY . .
RUN dotnet publish "DTNTB.API/DTNTB.API.csproj" \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
RUN apt-get update \
    && apt-get install --yes --no-install-recommends tzdata \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV TZ=Asia/Ho_Chi_Minh

USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "DTNTB.API.dll"]
