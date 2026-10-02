FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Tym.Api.csproj ./
RUN dotnet restore Tym.Api.csproj
COPY . ./
RUN dotnet publish Tym.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV TYM_MODEL_DIR=/app/models/api
ENV TYM_CORPUS_MODEL_DIR=/app/models/corpus
EXPOSE 8080
COPY --from=build /app/publish ./
COPY models/ ./models/
ENTRYPOINT ["dotnet", "Tym.Api.dll"]
