# VOD: nginx.conf и статика в образе — без монтирования каталога с диска Windows.
FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY content/ /usr/share/nginx/html/
