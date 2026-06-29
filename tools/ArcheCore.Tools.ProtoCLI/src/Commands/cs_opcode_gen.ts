import {CSharpOpCodes} from "../Lib/psOpcodes/proto.cs.ts";

export async function CSPersistenceOPCodeGen()
{
    CSharpOpCodes(process.env.WSERVER_P_OPCODEPATH);
}
