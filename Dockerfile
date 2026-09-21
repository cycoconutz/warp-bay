FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY WarpBay.Api/WarpBay.Api.csproj WarpBay.Api/
RUN dotnet restore WarpBay.Api/WarpBay.Api.csproj
COPY . .
RUN dotnet publish WarpBay.Api/WarpBay.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "WarpBay.Api.dll"]
