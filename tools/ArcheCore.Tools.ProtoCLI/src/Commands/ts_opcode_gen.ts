import fs from "fs";
import {tsEnum, TypescriptOPCodes} from "../Lib/psOpcodes/proto.ts.ts";
import * as dotenv from 'dotenv'
dotenv.config()


export async function TSPersistenceOPCodeGen()
{
    TypescriptOPCodes(process.env.PSERVER_OPCODEPATH)
}