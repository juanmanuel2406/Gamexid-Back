FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["Gamexid.Api/Gamexid.Api.csproj", "Gamexid.Api/"]
COPY ["Gamexid.Models/Gamexid.Models.csproj", "Gamexid.Models/"]
COPY ["Gamexid.Services/Gamexid.Services.csproj", "Gamexid.Services/"]
RUN dotnet restore "Gamexid.Api/Gamexid.Api.csproj"
COPY . .
RUN dotnet publish "Gamexid.Api/Gamexid.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Gamexid.Api.dll"]
