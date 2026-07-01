
import fs from "fs";
import {data, entries} from "../../lib.ts";

export const enumBody = entries
    .map(([name, value]) => `        ${name} = ${value},`)
    .join("\n");

export const csEnum =
    `namespace Shared 
{
    public enum ${data.namespace} : ${data.type}
    {
${enumBody}
    }  
}`;


export const CSharpOpCodes = (filePath:any) => {
    fs.writeFileSync(filePath, csEnum);
}