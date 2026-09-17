import net from "net";

import { decode }
    from "@msgpack/msgpack";
import {W2PConnectRequest} from "../../Shared/Packets/Requests/W2PConnectRequest";
import {SendP2WConnectResponse} from "../P2W/P2WConnectResponseSender";



export async function W2PConnectHandler(
    socket: net.Socket,
    payload: Uint8Array)
{
    const request =
        decode(payload) as W2PConnectRequest;

    console.log(
        `[Persistence] ${request.Message}`);

    SendP2WConnectResponse(
        socket);
}