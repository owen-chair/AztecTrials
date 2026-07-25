FROM alpine:3.20

RUN apk add --no-cache busybox-extras goaccess

ENTRYPOINT ["/usr/bin/goaccess"]
CMD ["--help"]
