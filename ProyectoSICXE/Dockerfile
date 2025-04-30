# Etapa base
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

# Etapa de build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copia TODO el repo al contenedor
COPY . .

# Busca el .csproj dentro de la carpeta correcta
WORKDIR /src/ProyectoSICXE
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# Etapa final
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ProyectoSICXE.dll"]
