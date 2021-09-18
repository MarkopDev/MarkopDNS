FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build-env
COPY . /src
WORKDIR /src
RUN dotnet publish -c Release -o /build


FROM mcr.microsoft.com/dotnet/runtime:6.0
WORKDIR /markop-dns
COPY --from=build-env /build .
CMD [ "dotnet", "MarkopDns.dll" ]