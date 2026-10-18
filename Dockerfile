FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Drogueria.csproj", "./"]
RUN dotnet restore "Drogueria.csproj"

COPY . .
RUN dotnet publish "Drogueria.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
EXPOSE 10000

ENTRYPOINT ["dotnet", "Drogueria.dll"]
