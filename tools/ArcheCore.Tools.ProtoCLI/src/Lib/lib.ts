import fs from "fs";
import YAML from 'yaml'
import * as dotenv from 'dotenv';
dotenv.config();



const yamlPath = process.env.YAML_SHARED_OPCODES_WS_PS;

if (!yamlPath) {
    throw new Error(
        'YAML_SHARED_OPCODES_WS_PS is missing from .env'
    );
}


export const file = fs.readFileSync(yamlPath, 'utf8')
export const data = YAML.parse(file)
export const entries = Object.entries(data.opcodes);