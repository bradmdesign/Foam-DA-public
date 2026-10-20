import { defineConfig } from 'vite';
import vinext from 'vinext';
export default defineConfig({ plugins:[vinext()], server:{host:'127.0.0.1',port:3000,watch:{ignored:['**/.cache/**','**/research/**','**/public/**','**/dist/**']}}, build:{chunkSizeWarningLimit:650} });
