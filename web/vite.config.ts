import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// API routes the dev server forwards to `dotnet run --project DatingApp.Api` (port 5199).
const api = ['/auth', '/me', '/users', '/discover', '/swipes', '/likes', '/matches', '/safety', '/photos', '/admin', '/health'];
const target = process.env.API_URL ?? 'http://localhost:5199';

export default defineConfig({
  plugins: [react()],
  // Served from /app so client routes never collide with API paths on refresh.
  base: '/app/',
  build: {
    // The API serves this folder, so one container ships both.
    outDir: '../DatingApp.Api/wwwroot/app',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      ...Object.fromEntries(api.map((path) => [path, { target, changeOrigin: true }])),
      '/hubs': { target, changeOrigin: true, ws: true },
    },
  },
});
