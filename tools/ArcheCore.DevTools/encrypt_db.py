"""
encrypt_gamedata.py

Encrypts a plaintext gamedata.db into the format GameDataCrypto.cs expects:
[16-byte random IV][AES-256-CBC ciphertext, PKCS7 padded]

Run this any time you update gamedata.db, on BOTH the copy you put in
StreamingAssets/GameData/ and the copy you drop next to the Authserver.
They must be encrypted with the same KEY value below, and that value must
exactly match the Key array in ArcheCore.Client/GameData/GameDataCrypto.cs.

Usage:
    python encrypt_gamedata.py path/to/plaintext_gamedata.db path/to/output_gamedata.db
"""

import sys
from pathlib import Path


from Crypto.Cipher import AES
from Crypto.Random import get_random_bytes
from Crypto.Util.Padding import pad, unpad

# Must exactly match the Key byte array in GameDataCrypto.cs (same 32 bytes,
# same order). If you rotate one, rotate the other and re-encrypt.
KEY = bytes([
    0x4B, 0x1C, 0x9E, 0x7A, 0x2D, 0x88, 0x3F, 0x61,
    0xA5, 0x0E, 0xD2, 0x77, 0x9B, 0x44, 0x1A, 0xC3,
    0x6F, 0x52, 0xE8, 0x09, 0xB1, 0x3D, 0x95, 0x2C,
    0x70, 0xF4, 0x18, 0x8A, 0x5C, 0xDB, 0x21, 0x67
])


def encrypt_file(input_path: Path, output_path: Path) -> None:
    plaintext = input_path.read_bytes()

    iv = get_random_bytes(16)
    cipher = AES.new(KEY, AES.MODE_CBC, iv)
    ciphertext = cipher.encrypt(pad(plaintext, AES.block_size))

    output_path.write_bytes(iv + ciphertext)
    print(f"Encrypted {input_path} ({len(plaintext)} bytes) -> "
          f"{output_path} ({len(iv) + len(ciphertext)} bytes)")



def main() -> None:
    if len(sys.argv) != 3:
        print("Usage: python encrypt_gamedata.py <input.db> <output.db>")
        sys.exit(1)

    input_path = Path(sys.argv[1])
    output_path = Path(sys.argv[2])

    if not input_path.exists():
        print(f"Input file not found: {input_path}")
        sys.exit(1)

    encrypt_file(input_path, output_path)


if __name__ == "__main__":
    main()