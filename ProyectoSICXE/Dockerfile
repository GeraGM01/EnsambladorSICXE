# Etapa 1: Construcción
# Usar la imagen base de .NET SDK
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build

# Definir el directorio de trabajo
WORKDIR /src

# Copiar los archivos del proyecto al contenedor
COPY EnsambladorSICXE/ProyectoSICXE/ProyectoSICXE.csproj EnsambladorSICXE/ProyectoSICXE/

# Restaurar las dependencias del proyecto
RUN dotnet restore EnsambladorSICXE/ProyectoSICXE/ProyectoSICXE.csproj

# Copiar el resto del código fuente
COPY EnsambladorSICXE/ProyectoSICXE/ EnsambladorSICXE/ProyectoSICXE/

# Compilar el proyecto
RUN dotnet build EnsambladorSICXE/ProyectoSICXE/ProyectoSICXE.csproj -c Release -o /app/build

# Publicar el proyecto
RUN dotnet publish EnsambladorSICXE/ProyectoSICXE/ProyectoSICXE.csproj -c Release -o /app/publish

# Etapa 2: Imagen final
# Usar la imagen base de .NET Runtime
FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS final

# Definir el directorio de trabajo en la imagen final
WORKDIR /app

# Copiar los archivos publicados desde la etapa de construcción
COPY --from=build /app/publish .

# Exponer el puerto en el que la app estará corriendo
EXPOSE 80

# Comando para ejecutar la aplicación
ENTRYPOINT ["dotnet", "ProyectoSICXE.dll"]
