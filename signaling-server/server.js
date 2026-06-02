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

    socket.on('disconnect', () => {
        const roomId = socket.data.roomId;
        if (roomId) socket.to(roomId).emit('user-left', { role: socket.data.role });
    });
});

const PORT = process.env.PORT || 3001;
httpServer.listen(PORT, () => {
    console.log(`Servidor de senalizacion escuchando en http://localhost:${PORT}`);
});
