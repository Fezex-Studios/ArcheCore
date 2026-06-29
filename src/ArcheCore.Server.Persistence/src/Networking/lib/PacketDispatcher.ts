import net from "net";

export type PacketHandler =
    (
        socket: net.Socket,
        payload: Uint8Array
    ) => Promise<void>;

const handlers =
    new Map<number, PacketHandler>();

export function RegisterHandler(
    opcode: number,
    handler: PacketHandler)
{
    handlers.set(
        opcode,
        handler);
}

export async function Dispatch(
    socket: net.Socket,
    opcode: number,
    payload: Uint8Array)
{
    const handler =
        handlers.get(opcode);

    if(!handler)
    {
        console.log(
            `Unknown Opcode ${opcode}`);

        return;
    }

    await handler(
        socket,
        payload);
}