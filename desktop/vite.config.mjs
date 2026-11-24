import {defineConfig} from 'vite';
import {fileURLToPath} from 'node:url';
export default defineConfig({root:fileURLToPath(new URL('./renderer',import.meta.url)),base:'./',build:{outDir:'../dist',emptyOutDir:true},server:{fs:{allow:[fileURLToPath(new URL('../',import.meta.url))]}}});
