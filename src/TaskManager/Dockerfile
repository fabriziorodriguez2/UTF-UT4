# Build context = raíz del repo (ver docker-compose.yml)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/TaskManager/TaskManager.csproj TaskManager/
RUN dotnet restore TaskManager/TaskManager.csproj
COPY src/TaskManager/ TaskManager/
RUN dotnet publish TaskManager/TaskManager.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "TaskManager.dll"]
