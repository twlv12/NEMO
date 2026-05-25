from http.server import SimpleHTTPRequestHandler
from http.server import HTTPServer

class Server(SimpleHTTPRequestHandler):

    def do_POST(self):
        if self.path == "/config":
            length = int(self.headers["Content-Length"])
            body = self.rfile.read(length)

            with open("runtimeConfig.json", "wb") as f:
                f.write(body)

            self.send_response(200)
            self.end_headers()

httpd = HTTPServer(("localhost", 8000), Server)
httpd.serve_forever()