FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copiar los archivos .csproj - rutas relativas desde la raíz del contexto
COPY ["VelsatBackendAPI/VelsatMobile.csproj", "VelsatBackendAPI/"]
COPY ["VelsatBackendAPI.Data/VelsatMobile.Data.csproj", "VelsatBackendAPI.Data/"]
COPY ["VelsatBackendAPI.Model/VelsatMobile.Model.csproj", "VelsatBackendAPI.Model/"]

# Restaurar dependencias
RUN dotnet restore "VelsatBackendAPI/VelsatMobile.csproj"

# Copiar todo el código desde la raíz
COPY . .

# Build del proyecto principal
WORKDIR "/src/VelsatBackendAPI"
RUN dotnet build "VelsatMobile.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "VelsatMobile.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "VelsatMobile.dll"]