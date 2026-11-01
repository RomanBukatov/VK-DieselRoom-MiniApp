# Stage 1: Build Vite / React app with Bun
FROM oven/bun:alpine AS build
WORKDIR /app

COPY package.json bun.lock* ./
RUN bun install --frozen-lockfile || bun install

COPY . .
# Ссылка на мини-приложение для реферальных приглашений (#ref=...)
ARG VITE_VK_APP_URL=
ENV VITE_VK_APP_URL=$VITE_VK_APP_URL
RUN bun run build

# Stage 2: Production Nginx Server
FROM nginx:alpine AS runtime

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
