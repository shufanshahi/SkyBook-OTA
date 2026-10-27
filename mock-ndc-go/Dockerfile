FROM golang:1.23-alpine AS build
WORKDIR /app
COPY go.mod ./
COPY *.go ./
RUN CGO_ENABLED=0 go build -o mockndc .

FROM alpine:3.20
COPY --from=build /app/mockndc /mockndc
EXPOSE 8081
ENTRYPOINT ["/mockndc"]
