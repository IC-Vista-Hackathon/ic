# syntax=docker/dockerfile:1
FROM node:24-alpine AS build
WORKDIR /src
COPY frontends/IC.BillerExperience.Studio/package.json frontends/IC.BillerExperience.Studio/package-lock.json ./
RUN npm ci
COPY frontends/IC.BillerExperience.Studio/ ./
ARG VITE_API_URL=/api
ENV VITE_API_URL=$VITE_API_URL
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.29-alpine AS final
COPY deploy/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist /usr/share/nginx/html
EXPOSE 8080
