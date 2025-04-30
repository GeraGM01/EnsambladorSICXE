# Usa la imagen SDK para compilar
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# Copia el contenido del repositorio
COPY . .

# Cambia a la carpeta del proyecto
WORKDIR /src/ProyectoSICXE

#  Restaurar dependencias del proyecto explícitamente
RUN dotnet restore ProyectoSICXE.csproj

# Compilar la aplicación
RUN dotnet publish ProyectoSICXE.csproj -c Release -o /app/publish

# Usa la imagen de runtime para producción
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

WORKDIR /app
COPY --from=build /app/publish .

# Comando para ejecutar la app
ENTRYPOINT ["dotnet", "ProyectoSICXE.dll"]
