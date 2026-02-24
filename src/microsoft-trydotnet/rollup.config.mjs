// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import typescript from 'rollup-plugin-typescript2'
import ts from 'typescript';
import resolve from '@rollup/plugin-node-resolve';
import commonjs from '@rollup/plugin-commonjs';
import { readFileSync } from 'fs';
const pkg = JSON.parse(readFileSync(new URL('./package.json', import.meta.url)));

export default {

    input: 'src/index.ts',
    output: [
        {
            sourcemap: 'inline',
            file: pkg.main,
            format: 'umd',
            name: 'trydotnet',
        },
        {
            sourcemap: 'inline',
            file: pkg.module,
            format: 'es',
        }
    ],
    external: [
        //  ...Object.keys(pkg.dependencies || {}),
        ...Object.keys(pkg.peerDependencies || {}),
        ...Object.keys(pkg.devDependencies || {}),
    ],
    plugins: [
        typescript({
            typescript: ts,
            tsconfigOverride: {
                compilerOptions: {
                    "module": "ES2015"
                }
            },
        }),
        resolve({
            jsnext: true,
            main: true,
            browser: true,
            customResolveOptions: {
                moduleDirectories: ['node_modules']
            }
        }),
        commonjs()
    ],
}