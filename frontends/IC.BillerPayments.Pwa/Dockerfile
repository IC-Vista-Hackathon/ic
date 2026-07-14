# syntax=docker/dockerfile:1
FROM node:24-alpine AS build
WORKDIR /src
COPY frontends/IC.BillerPayments.Pwa/package.json frontends/IC.BillerPayments.Pwa/package-lock.json ./
RUN npm ci
COPY frontends/IC.BillerPayments.Pwa/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.29-alpine AS final
COPY deploy/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist /usr/share/nginx/html
EXPOSE 8080
