# Markop Proxy
A DNS server and SNI proxy server help proxy the HTTP and packet over TLS traffic just with config DNS on the client-side.

## What is the SNI Proxy Server?
SNI Proxy Server proxy incoming traffic based on the hostname contained in the TCP session.
For example, in HTTPv1 protocol with a Host header to determine the actual destination of packets and TLS protocol with the help of server name extension, the proxy server determines the actual destination.

## How does Markop Proxy work?
This flow describe how client send a Http request to example.com server
![](MarkopProxy.png)
1. Client setup Markop DNS server as OS default DNS server
2. Client OS sends DNS query to Markop DNS server to resolve example.com into IPv4
3. Markop DNS server checks its white list record and resolve example.com into Markop Proxy server IP
4. OS sends a Http request to Markop Proxy server IP
   5.1. Markop Proxy server parse and analyze the data to extract request hostname(example.com)
   5.2. Markop Proxy server sends data into hostname(example.com)
6. Example.com server sends response to Markop Proxy server
7. Markop Proxy server sends response to client OS

## Feature
- Support HTTPv1, TLS protocols
- Multiple listening sockets

## Usage
```sh
$ git clone https://github.com/MarkopDev/MarkopProxy
$ cd MarkopProxy/MarkopProxy
$ dotnet publish -c Release -o build
```

### Build Project
```sh
$ dotnet publish -c Release -o build
```

### Docker
```sh
$ docker-compose up -d
```
or
```sh
$ docker build -t markop-proxy .
$ docker run -d markop-proxy
```

## Contributions
If you're interested in contributing to this project, first of all, We would like to extend my heartfelt gratitude. \
Please feel free to reach out to us if you need help.

## LICENSE
MIT