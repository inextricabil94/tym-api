FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/Tym.Corpus.Api/Tym.Corpus.Api.csproj src/Tym.Corpus.Api/
COPY src/Tym.Corpus.Core/Tym.Corpus.Core.csproj src/Tym.Corpus.Core/
RUN dotnet restore src/Tym.Corpus.Api/Tym.Corpus.Api.csproj
COPY src/ src/
RUN dotnet publish src/Tym.Corpus.Api/Tym.Corpus.Api.csproj -c Release --no-restore -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /publish ./
COPY models/ ./models/
ENV ASPNETCORE_HTTP_PORTS=8080
ENV TYM_CORPUS_MODEL_DIR=/app/models
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Tym.Corpus.Api.dll"]
