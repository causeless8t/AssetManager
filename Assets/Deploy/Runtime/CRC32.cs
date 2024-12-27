using System.IO;
using System.Text;

namespace Causeless3t.Security
{
    public static class CRC32
    {
        private static readonly uint[] Table;

        // 테이블 초기화
        static CRC32()
        {
            const uint polynomial = 0xedb88320;
            Table = new uint[256];

            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 8; j > 0; j--)
                {
                    if ((crc & 1) == 1)
                    {
                        crc = (crc >> 1) ^ polynomial;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }

                Table[i] = crc;
            }
        }

        // CRC32 계산 (바이트 배열 입력)
        public static uint Compute(byte[] data)
        {
            uint crc = 0xffffffff;

            foreach (byte b in data)
            {
                byte tableIndex = (byte)((crc ^ b) & 0xff);
                crc = (crc >> 8) ^ Table[tableIndex];
            }

            return ~crc;
        }

        // CRC32 계산 (문자열 입력)
        public static uint Compute(string input)
        {
            byte[] data = Encoding.UTF8.GetBytes(input);
            return Compute(data);
        }

        // CRC32 계산 (FileStream 입력)
        public static uint Compute(FileStream stream)
        {
            uint crc = 0xffffffff;
            int bytesRead;
            byte[] buffer = new byte[4096]; // 4KB 버퍼

            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < bytesRead; i++)
                {
                    byte tableIndex = (byte)((crc ^ buffer[i]) & 0xff);
                    crc = (crc >> 8) ^ Table[tableIndex];
                }
            }

            return ~crc;
        }
    }
}
