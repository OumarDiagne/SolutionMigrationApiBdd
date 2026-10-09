#!/usr/bin/env bash
# Crée 20 produits de démonstration sur l'API déployée (Azure Container Apps).
#
# Pourquoi passer par l'API plutôt que par un INSERT SQL : la création d'un produit écrit aussi
# le mouvement de stock « stock initial » et la ligne du journal d'audit. Un INSERT direct
# laisserait ces traces vides et fausserait la traçabilité.
#
# Utilisation (Azure Cloud Shell, connecté avec `az login`) :
#   export ADMIN_EMAIL="<e-mail du compte administrateur de l'API>"
#   bash scripts/seed-demo-produits.sh
#
# Variables facultatives : API_URL (adresse de l'API), VAULT_NAME (nom du coffre Key Vault).
# Le mot de passe admin est lu dans Key Vault (secret `adminpwd`) : il n'est ni saisi ni affiché.
#
# Rejouable sans doublon : un produit dont le nom existe déjà est ignoré, et chaque création
# porte une clé d'idempotence fixe (demo-produit-NN).
set -euo pipefail

API_URL="${API_URL:-https://ca-migapi.salmonriver-8486a327.francecentral.azurecontainerapps.io}"
VAULT_NAME="${VAULT_NAME:-kv-migapi-od}"
: "${ADMIN_EMAIL:?Définir ADMIN_EMAIL (e-mail du compte administrateur de l’API)}"

command -v jq >/dev/null || { echo "jq est requis (présent dans Azure Cloud Shell)." >&2; exit 1; }

# Nom | Description | Prix unitaire TTC | Stock initial
PRODUITS=(
  "Clavier mécanique sans fil|Clavier mécanique compact, rétroéclairé, connexion Bluetooth et USB-C.|89.90|40"
  "Souris ergonomique verticale|Souris verticale sans fil pour limiter la fatigue du poignet.|49.90|60"
  "Écran 27 pouces QHD|Moniteur IPS 27 pouces, résolution 2560x1440, 75 Hz.|279.00|25"
  "Webcam Full HD|Webcam 1080p avec microphone intégré et cache de confidentialité.|59.90|50"
  "Casque audio à réduction de bruit|Casque Bluetooth, réduction de bruit active, 30 heures d'autonomie.|149.00|30"
  "Hub USB-C 7 en 1|Hub HDMI, 3 ports USB-A, lecteur SD et microSD, charge 100 W.|39.90|80"
  "Support d'ordinateur portable|Support aluminium réglable en hauteur, compatible 10 à 17 pouces.|34.90|70"
  "Tapis de souris XXL|Tapis 90x40 cm, surface lisse, base antidérapante.|19.90|120"
  "Lampe de bureau LED|Lampe LED à intensité et température de couleur réglables.|44.90|45"
  "Chaise de bureau ergonomique|Chaise à soutien lombaire réglable, accoudoirs 3D, assise respirante.|229.00|15"
  "Bureau assis-debout|Bureau électrique réglable en hauteur, plateau 140x70 cm.|399.00|10"
  "Câble HDMI 2.1 de 2 m|Câble haut débit 8K, connecteurs plaqués or.|14.90|150"
  "Disque SSD externe 1 To|SSD portable USB 3.2, vitesse de lecture jusqu'à 1000 Mo/s.|99.90|35"
  "Clé USB 128 Go|Clé USB 3.0 en métal, 128 Go.|16.90|200"
  "Batterie externe 20000 mAh|Batterie portable avec deux ports USB-C et un port USB-A.|42.90|55"
  "Routeur Wi-Fi 6|Routeur double bande Wi-Fi 6, quatre ports Gigabit.|89.00|28"
  "Enceinte Bluetooth portable|Enceinte étanche IPX7, 12 heures d'autonomie.|59.00|65"
  "Cahier de notes A5 à spirale|Carnet de 120 pages à petits carreaux, couverture rigide.|6.90|300"
  "Lot de 10 stylos gel|Stylos à encre gel noire, pointe 0,5 mm.|9.90|250"
  "Sac à dos pour ordinateur 15 pouces|Sac déperlant avec compartiment matelassé et port USB de charge.|54.90|40"
)

echo "Lecture du mot de passe admin dans Key Vault ($VAULT_NAME)..."
ADMIN_PASSWORD="$(az keyvault secret show --vault-name "$VAULT_NAME" --name adminpwd --query value -o tsv)"
export ADMIN_EMAIL ADMIN_PASSWORD

echo "Connexion à l'API ($API_URL). Le premier appel peut prendre environ une minute (réveil de l'API et de la base)..."
TOKEN="$(jq -n '{email: env.ADMIN_EMAIL, password: env.ADMIN_PASSWORD}' \
  | curl -fsS --retry 10 --retry-delay 15 --retry-all-errors \
      -X POST "$API_URL/api/Auth/login" \
      -H "Content-Type: application/json" --data @- \
  | jq -r '.accessToken')"
unset ADMIN_PASSWORD

[ -n "$TOKEN" ] && [ "$TOKEN" != "null" ] || { echo "Connexion refusée : jeton absent." >&2; exit 1; }

echo "Lecture du catalogue existant..."
EXISTANTS="$(curl -fsS "$API_URL/api/Produit" -H "Authorization: Bearer $TOKEN" | jq -r '.[].nomProduit')"

cree=0; ignore=0; i=0
for ligne in "${PRODUITS[@]}"; do
  i=$((i + 1))
  IFS='|' read -r nom description prix stock <<< "$ligne"

  if grep -Fxq -- "$nom" <<< "$EXISTANTS"; then
    echo "  [ignoré]  $nom (déjà présent)"
    ignore=$((ignore + 1))
    continue
  fi

  corps="$(jq -n --arg n "$nom" --arg d "$description" --argjson p "$prix" --argjson s "$stock" \
    '{nomProduit: $n, description: $d, prixUnitaireTTC: $p, stock: $s}')"

  code="$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$API_URL/api/Produit" \
    -H "Authorization: Bearer $TOKEN" \
    -H "Content-Type: application/json" \
    -H "Idempotency-Key: demo-produit-$(printf '%02d' "$i")" \
    --data "$corps")"

  if [ "$code" = "201" ]; then
    echo "  [créé]    $nom"
    cree=$((cree + 1))
  else
    echo "  [ERREUR $code] $nom" >&2
    exit 1
  fi
done

echo "Terminé : $cree produit(s) créé(s), $ignore ignoré(s)."
