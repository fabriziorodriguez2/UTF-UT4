# Build context = raíz del repo (ver docker-compose.yml)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/ProjectManager/ProjectManager.csproj ProjectManager/
RUN dotnet restore ProjectManager/ProjectManager.csproj
COPY src/ProjectManager/ ProjectManager/
RUN dotnet publish ProjectManager/ProjectManager.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "ProjectManager.dll"]
