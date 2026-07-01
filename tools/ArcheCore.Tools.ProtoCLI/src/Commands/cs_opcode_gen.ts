import {CSharpOpCodes} from "../Lib/GenOpcodes/psOpcodes/proto.cs.ts";

export async function CSPersistenceOPCodeGen()
{
    CSharpOpCodes(process.env.WSERVER_P_OPCODEPATH);
}
