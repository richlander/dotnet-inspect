# Native AOT container for the MSDL CORS proxy. Multi-stage: build+publish
# with the SDK image, then copy the trimmed native binary into a minimal
# runtime-deps image with no managed .NET runtime at all.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY MsdlProxy.csproj .
RUN dotnet restore MsdlProxy.csproj -r linux-x64
COPY . .
RUN dotnet publish MsdlProxy.csproj -c Release -r linux-x64 --self-contained -o /app

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/MsdlProxy .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["./MsdlProxy"]
