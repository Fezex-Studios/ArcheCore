import Fastify   from "fastify";
import cors      from "@fastify/cors";
import rateLimit from "@fastify/rate-limit";
import { FastifyRequest } from "fastify";

import "./Database/Database";

import { RegisterRoute }        from "./Routes/RegisterRoute";
import { LoginRoute }           from "./Routes/LoginRoute";
import { ValidateSessionRoute } from "./Routes/ValidateSessionRoute";
import { GameDataRoute }        from "./Routes/GameDataRoute";

const server = Fastify({ logger: true });

server.register(cors, {
    origin:  true,
    methods: ["GET", "POST"],
});

server.register(rateLimit, {
    max:        10,
    timeWindow: "1 minute",
    // allowList accepts a function — return true to skip rate limiting for that request
    allowList: (request: FastifyRequest) => request.url === "/validate-session",
});

server.register(RegisterRoute);
server.register(LoginRoute);
server.register(ValidateSessionRoute);
server.register(GameDataRoute);

server
    .listen({ port: Number(process.env.PORT ?? 3000), host: "0.0.0.0" })
    .then(() => console.log("Auth Server Ready"));