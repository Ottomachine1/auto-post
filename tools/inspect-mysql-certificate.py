"""Unauthenticated certificate inspection only. Never sends MySQL credentials.

The inspection TLS context cannot authenticate the endpoint. Compare the output
with the server-side certificate through an independently trusted channel.
This is not an application connection or a trust/pinning decision.
"""
import hashlib
import socket
import ssl
import struct
import sys

host = sys.argv[1]
port = int(sys.argv[2])
with socket.create_connection((host, port), timeout=8) as sock:
    def read_exact(count):
        data = b''
        while len(data) < count:
            part = sock.recv(count - len(data))
            if not part:
                raise RuntimeError('Unexpected MySQL handshake EOF')
            data += part
        return data
    header = read_exact(4)
    greeting = read_exact(int.from_bytes(header[:3], 'little'))
    version = greeting[1:].split(b'\0', 1)[0].decode('ascii', 'replace')
    capabilities = 0x00000200 | 0x00000800 | 0x00008000 | 0x00080000
    request = struct.pack('<IIB23s', capabilities, 16 * 1024 * 1024, 45, b'')
    sock.sendall(len(request).to_bytes(3, 'little') + b'\x01' + request)
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    context.check_hostname = False
    context.verify_mode = ssl.CERT_NONE  # Inspection only; no authentication follows.
    with context.wrap_socket(sock, server_hostname=host) as tls:
        certificate = tls.getpeercert(binary_form=True)
        digest = hashlib.sha256(certificate).hexdigest().upper()
        print('Endpoint:', host, port)
        print('MySQL:', version, 'TLS:', tls.version())
        print('SHA-256:', ':'.join(digest[i:i+2] for i in range(0, len(digest), 2)))
        print('No credentials sent; endpoint identity is NOT verified.')
