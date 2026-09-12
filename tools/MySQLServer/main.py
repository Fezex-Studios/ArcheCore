import os
import sys
import subprocess
import ctypes

SERVICE_NAME = "LocalMySQLServer"  # Your service name


def is_admin():
    """Check if running as admin"""
    try:
        return ctypes.windll.shell32.IsUserAnAdmin()
    except:
        return False


def run_as_admin():
    """Restart as admin"""
    ctypes.windll.shell32.ShellExecuteW(None, "runas", sys.executable,
                                        " ".join(sys.argv), None, 1)
    sys.exit()


def clear():
    os.system('cls' if os.name == 'nt' else 'clear')


def check_status():
    """Check if MySQL service is running"""
    try:
        result = subprocess.run(['sc', 'query', SERVICE_NAME],
                                capture_output=True, text=True, shell=True)
        return 'RUNNING' in result.stdout
    except:
        return False


def main():
    # Require admin
    if not is_admin():
        print("Restarting as administrator...")
        run_as_admin()
        return

    while True:
        clear()
        status = "🟢 RUNNING" if check_status() else "🔴 STOPPED"

        print("╔════════════════════════════════════╗")
        print("║     MySQL Service Controller       ║")
        print("╚════════════════════════════════════╝")
        print(f"\nService: {SERVICE_NAME}")
        print(f"Status:  {status}")
        print("\n" + "═" * 40)
        print("1. Start MySQL")
        print("2. Stop MySQL")
        print("3. Open MySQL Shell")
        print("4. Open Services Panel")
        print("5. Exit")
        print("═" * 40)

        choice = input("\nSelect option (1-5): ").strip()

        if choice == '1':
            print(f"\nStarting {SERVICE_NAME}...")
            result = subprocess.run(['net', 'start', SERVICE_NAME],
                                    capture_output=True, text=True, shell=True)
            print(result.stdout)
            if result.stderr:
                print(f"Error: {result.stderr}")
            input("\nPress Enter to continue...")

        elif choice == '2':
            print(f"\nStopping {SERVICE_NAME}...")
            result = subprocess.run(['net', 'stop', SERVICE_NAME],
                                    capture_output=True, text=True, shell=True)
            print(result.stdout)
            if result.stderr:
                print(f"Error: {result.stderr}")
            input("\nPress Enter to continue...")

        elif choice == '3':
            print("\nOpening MySQL Shell...")
            # Two options - choose one:

            # Option A: Open in new window (keeps window open)
            subprocess.Popen(['start', 'cmd', '/k', 'mysql -u root -p'],
                             shell=True)

            # Option B: Open in current window (will close after exit)
            # subprocess.run(['mysql', '-u', 'root', '-p'])

        elif choice == '4':
            print("\nOpening Services Panel...")
            subprocess.Popen(['services.msc'], shell=True)

        elif choice == '5':
            print("\nGoodbye!")
            sys.exit()

        else:
            print("\nInvalid choice!")
            input("Press Enter to continue...")


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\n\nExiting...")
    except Exception as e:
        print(f"\nError: {e}")
        input("Press Enter to exit...")