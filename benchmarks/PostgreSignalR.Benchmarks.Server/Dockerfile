FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the rest of the source
COPY benchmarks/ benchmarks/

# Restore just the server project (pulls in contracts)
RUN dotnet restore benchmarks/PostgreSignalR.Benchmarks.Server/PostgreSignalR.Benchmarks.Server.csproj

# Publish
RUN dotnet publish benchmarks/PostgreSignalR.Benchmarks.Server/PostgreSignalR.Benchmarks.Server.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /out .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "PostgreSignalR.Benchmarks.Server.dll"]
