import net from "net";
import { decode } from "@msgpack/msgpack";

import "./Database/Database";

import type { Packet }
    from "./Shared/Packet";

import {
    Dispatch
} from "./Networking/lib/PacketDispatcher";

import {
    RegisterHandlers
} from "./Networking/RegisterHandlers";

RegisterHandlers();

const server =
    net.createServer(socket =>
    {
        console.log(
            "World Server Connected");

        let buffer =
            Buffer.alloc(0);

        socket.on(
            "data",
            async (data: Buffer) =>
            {
                buffer =
                    Buffer.concat([
                        buffer,
                        data
                    ]);

                while(buffer.length >= 4)
                {
                    const length =
                        buffer.readInt32LE(
                            0);

                    if(buffer.length < length + 4)
                    {
                        break;
                    }

                    const packetBytes =
                        buffer.subarray(
                            4,
                            4 + length);

                    buffer =
                        buffer.subarray(
                            4 + length);

                    const packet: Packet =
                        decode(packetBytes) as Packet;

                    await Dispatch(
                        socket,
                        packet.Opcode,
                        packet.Payload);
                }
            });

        socket.on(
            "close",
            () =>
            {
                console.log(
                    "World Server Disconnected");
            });

        socket.on(
            "error",
            error =>
            {
                console.error(
                    "Socket Error:",
                    error);
            });
    });

server.listen(
    7778,
    () =>
    {
        console.log(
            "Persistence Server Listening");
    });