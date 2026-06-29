

import * as dotenv from 'dotenv'
import { ShowMenu } from "./Menu/Menu.js";

import {CSPersistenceOPCodeGen} from "./Commands/cs_opcode_gen.js";
import {TSPersistenceOPCodeGen} from "./Commands/ts_opcode_gen.js";

//dotenv.config()
//TypescriptOPCodes(process.env.PServerOpCodes)
//CSharpOpCodes(process.env.WServerOPCodes)

const actions = {
    cs_persistence_opcode_gen: CSPersistenceOPCodeGen,
    ts_persistence_opcode_gen: TSPersistenceOPCodeGen
};

import { input } from '@inquirer/prompts';

while (true)
{
    console.clear();

    const answer = await ShowMenu();

    if (answer === "exit")
        break;

    console.clear();

    await actions[answer]?.();

    await input({
        message: "Press Enter to continue..."
    });
}