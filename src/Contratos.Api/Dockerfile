FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore em camada própria: só refaz quando os .csproj mudam.
COPY Directory.Build.props Directory.Packages.props Contratos.sln ./
COPY src/Contratos.Domain/*.csproj src/Contratos.Domain/
COPY src/Contratos.Application/*.csproj src/Contratos.Application/
COPY src/Contratos.Infrastructure/*.csproj src/Contratos.Infrastructure/
COPY src/Contratos.Api/*.csproj src/Contratos.Api/
RUN dotnet restore src/Contratos.Api
COPY src/ src/
RUN dotnet publish src/Contratos.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# O volume de chaves do Data Protection herda o dono deste diretório.
RUN mkdir -p /data/keys && chown app:app /data/keys
USER app
ENV ASPNETCORE_HTTP_PORTS=7700
EXPOSE 7700
ENTRYPOINT ["dotnet", "Contratos.Api.dll"]
