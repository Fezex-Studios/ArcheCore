import fs from "fs";
import {data, entries} from "../lib.ts";

export const tsEnum =
    `export enum ${data.namespace}
{\n` +
    entries
        .map(([name,value])=> `    ${name} = ${value},`)
        .join('\n') +
    `\n
}`;

export const TypescriptOPCodes = (filePath:any) => {
    fs.writeFileSync(filePath, tsEnum)
}