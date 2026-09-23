import { defineConfig } from 'vite'

// https://vitejs.dev/config/
export default defineConfig({
    // Relative asset paths: the built site works from the root of a server or from a subdirectory.
    base: './',
    clearScreen: false,
    server: {
        watch: {
            ignored: [
                "**/*.fs" // Don't watch F# files
            ]
        }
    }
})
