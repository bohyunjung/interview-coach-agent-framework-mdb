## Configure git
echo Configure git
git config --global pull.rebase false
git config --global core.autocrlf input

## Install .NET dev certs
echo Install .NET dev certs
dotnet dev-certs https --trust

## Install Aspire CLI
echo Install Aspire CLI
curl -sSL https://aspire.dev/install.sh | bash

## Install Python dependencies for the memory seed scripts
echo Install Python dependencies
python -m pip install --quiet -r scripts/memory/requirements.txt

echo DONE!
