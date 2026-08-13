FROM node:24-alpine AS build
WORKDIR /app
COPY apps/web/package*.json ./
RUN npm ci
COPY apps/web/ .
RUN npm run build

FROM nginx:1.29-alpine
COPY apps/web/nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html
ENV API_BASE_URL=http://api:8080
ENV NGINX_ENVSUBST_FILTER=API_BASE_URL
EXPOSE 80
