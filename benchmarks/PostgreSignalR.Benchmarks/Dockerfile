FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the rest of the source
COPY benchmarks/ benchmarks/

# Restore just the driver project (pulls in contracts)
RUN dotnet restore benchmarks/PostgreSignalR.Benchmarks/PostgreSignalR.Benchmarks.csproj

# Publish
RUN dotnet publish benchmarks/PostgreSignalR.Benchmarks/PostgreSignalR.Benchmarks.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app

COPY --from=build /out .
ENTRYPOINT ["dotnet", "PostgreSignalR.Benchmarks.dll"]
