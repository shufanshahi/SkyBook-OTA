# build from the repo root: docker build -f src/SkyBook.MockGds/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/SkyBook.MockGds/ src/SkyBook.MockGds/
RUN dotnet publish src/SkyBook.MockGds -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
EXPOSE 8080
ENTRYPOINT ["dotnet", "SkyBook.MockGds.dll"]
