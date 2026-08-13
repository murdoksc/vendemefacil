FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["apps/api/VendemeFacil.Api.csproj", "apps/api/"]
RUN dotnet restore "apps/api/VendemeFacil.Api.csproj"
COPY . .
RUN dotnet publish "apps/api/VendemeFacil.Api.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "VendemeFacil.Api.dll"]
