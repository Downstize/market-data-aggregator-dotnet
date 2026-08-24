FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "src/Simulators/BrokerPilot.ExchangeSimulator.Alpha/BrokerPilot.ExchangeSimulator.Alpha.csproj"
RUN dotnet publish "src/Simulators/BrokerPilot.ExchangeSimulator.Alpha/BrokerPilot.ExchangeSimulator.Alpha.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "BrokerPilot.ExchangeSimulator.Alpha.dll"]
