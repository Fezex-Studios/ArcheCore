import { select } from "@inquirer/prompts";

export async function ShowMenu()
{
    return await select({
        message: "ArcheCore Protocol Generator",
        choices: [
            {
                name: "CS-PersistenceOPCode Generator",
                value: "cs_persistence_opcode_gen",
                description: "Generates the .cs opcode file to the set dir",
            },
            {
                name: "TS-PersistenceOPCode Generator",
                value: "ts_persistence_opcode_gen",
                description: "Generates .ts opcode file to the set dir",
            },
            {
                name: "Exit",
                value: "exit"
            }
        ]
    });
}