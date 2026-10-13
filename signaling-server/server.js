const { createServer } = require('http');
const { Server } = require('socket.io');

const httpServer = createServer((req, res) => {
    if (req.url === '/health') {
        res.writeHead(200, { 'Content-Type': 'text/plain' });
        res.end('ok');
        return;
    }
    res.writeHead(404);
    res.end();
});

const io = new Server(httpServer, {
    cors: { origin: '*', methods: ['GET', 'POST'] }
});

io.on('connection', (socket) => {
    socket.on('join-room', ({ roomId, role }) => {
        socket.data.roomId = roomId;
        socket.data.role = role;
        socket.join(roomId);
        socket.emit('joined', { roomId });
        socket.to(roomId).emit('user-joined', { role });
    });

    socket.on('offer', ({ roomId, offer }) => {
        socket.to(roomId).emit('offer', { offer });
    });

    socket.on('answer', ({ roomId, answer }) => {
        socket.to(roomId).emit('answer', { answer });
    });

    socket.on('ice-candidate', ({ roomId, candidate }) => {
        socket.to(roomId).emit('ice-candidate', { candidate });
    });

    // Circuito de atención: avisos de cambio en los interruptores de IA y transcripción.
    // Solo dicen "consultá de nuevo": el estado real lo da la API (GET /salas/{codigo}), que
    // sí valida quién es quién. Nada de lo que viaje por acá se toma como verdad.
    socket.on('funciones-cambio', ({ roomId }) => {
        if (roomId && roomId === socket.data.roomId) socket.to(roomId).emit('funciones-cambio');
    });
    socket.on('pedido-apagado', ({ roomId, funcion }) => {
        if (roomId && roomId === socket.data.roomId) socket.to(roomId).emit('pedido-apagado', { funcion: String(funcion || '') });
    });

    socket.on('disconnect', () => {
        const roomId = socket.data.roomId;
        if (roomId) socket.to(roomId).emit('user-left', { role: socket.data.role });
    });
});

const PORT = process.env.PORT || 3001;
httpServer.listen(PORT, () => {
    console.log(`Servidor de senalizacion escuchando en http://localhost:${PORT}`);
});
