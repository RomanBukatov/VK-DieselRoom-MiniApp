# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Файлы проектов отдельно — кэширование NuGet-пакетов
COPY DieselRoom.Domain/DieselRoom.Domain.csproj DieselRoom.Domain/
COPY DieselRoom.Infrastructure/DieselRoom.Infrastructure.csproj DieselRoom.Infrastructure/
COPY DieselRoom.Api/DieselRoom.Api.csproj DieselRoom.Api/

RUN dotnet restore DieselRoom.Api/DieselRoom.Api.csproj

COPY DieselRoom.Domain/ DieselRoom.Domain/
COPY DieselRoom.Infrastructure/ DieselRoom.Infrastructure/
COPY DieselRoom.Api/ DieselRoom.Api/

WORKDIR /src/DieselRoom.Api
RUN dotnet publish DieselRoom.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 5062
ENV ASPNETCORE_URLS=http://+:5062

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "DieselRoom.Api.dll"]
