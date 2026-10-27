# build from the repo root: docker build -f src/SkyBook.Web/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY SkyBook.sln .
COPY src/ src/
COPY db/procedures/ db/procedures/
RUN dotnet publish src/SkyBook.Web -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
EXPOSE 8080
ENTRYPOINT ["dotnet", "SkyBook.Web.dll"]
