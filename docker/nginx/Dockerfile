# L7 nginx: конфиги копируются в образ вместо bind-mount с хоста (совместимость с Docker Desktop на Windows).
FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/nginx.conf
COPY nginx-l7.conf /etc/nginx/conf.d/default.conf
