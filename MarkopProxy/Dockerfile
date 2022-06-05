FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build-env
COPY . /src
WORKDIR /src
RUN dotnet publish -c Release -r linux-x64 -p:PublishReadyToRun=true --self-contained -o /build


FROM mcr.microsoft.com/dotnet/runtime:6.0
WORKDIR /markop-proxy
COPY --from=build-env /build .
CMD [ "./MarkopProxy" ]