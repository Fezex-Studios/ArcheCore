# decrypt_gamedata.py

import sys
from pathlib import Path
from Crypto.Cipher import AES
from Crypto.Util.Padding import unpad

# Must match encrypt_gamedata.py
KEY = bytes([
    0x4B, 0x1C, 0x9E, 0x7A, 0x2D, 0x88, 0x3F, 0x61,
    0xA5, 0x0E, 0xD2, 0x77, 0x9B, 0x44, 0x1A, 0xC3,
    0x6F, 0x52, 0xE8, 0x09, 0xB1, 0x3D, 0x95, 0x2C,
    0x70, 0xF4, 0x18, 0x8A, 0x5C, 0xDB, 0x21, 0x67
])

def decrypt_file(input_path: Path, output_path: Path):
    data = input_path.read_bytes()

    iv = data[:16]
    ciphertext = data[16:]

    cipher = AES.new(KEY, AES.MODE_CBC, iv)
    plaintext = unpad(
        cipher.decrypt(ciphertext),
        AES.block_size
    )

    output_path.write_bytes(plaintext)

    print(f"Decrypted -> {output_path}")

def main():
    if len(sys.argv) != 3:
        print("Usage: python decrypt_gamedata.py encrypted.db output.db")
        return

    decrypt_file(
        Path(sys.argv[1]),
        Path(sys.argv[2])
    )

if __name__ == "__main__":
    main()