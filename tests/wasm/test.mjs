// Test WASM module loading and Compress function via Node.js
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const bundleDir = join(__dirname, '../../src/NinePatch.Wasm/bin/Debug/net10.0/browser-wasm/AppBundle');

// Load the dotnet JS runtime/loader
const DOTNET = await import(join('file://', bundleDir, 'dotnet.js'));

async function runTest() {
    console.log('Loading WASM module...');

    const dotnet = await DOTNET.default();

    // Wait for runtime to be ready
    await dotnet.runMain('NinePatch.Wasm.dll');

    console.log('WASM module loaded successfully.');

    // Test GetVersion
    const version = dotnet.Module.exports.GetVersion();
    console.log(`Version: ${version}`);

    // Test Compress with a simple 4x4 solid color image
    const w = 4, h = 4;
    const rgba = new Uint8Array(w * h * 4);
    for (let i = 0; i < w * h; i++) {
        rgba[i * 4 + 0] = 128; // R
        rgba[i * 4 + 1] = 128; // G
        rgba[i * 4 + 2] = 128; // B
        rgba[i * 4 + 3] = 255; // A
    }

    console.log(`Testing Compress with ${w}x${h} solid color image...`);

    // Call Compress via JS interop
    const resultStr = dotnet.Module.exports.Compress(
        rgba.buffer, rgba.byteOffset, rgba.byteLength,
        w, h, 4.0, 0, 30.0
    );

    console.log('Result:', resultStr);
    console.log('WASM test passed!');
}

runTest().catch(err => {
    console.error('Test failed:', err);
    process.exit(1);
});
